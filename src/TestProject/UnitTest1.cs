using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace TestProject
{
    public class UnitTest1
    {
        private ITestOutputHelper _outputHelper;

        public UnitTest1(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public void ExceptionCallStack_InsideCatch()
        {
            List<Exception> assertExceptions = new();

            EventHandler<FirstChanceExceptionEventArgs> handler =
                (s, e) => FirstChanceExceptionCallback(e.Exception, assertExceptions, _outputHelper);

            AppDomain.CurrentDomain.FirstChanceException += handler;
            try
            {
                ThrowAndCatchEclipsingExceptionInsideCatch();
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException -= handler;
            }

            if (assertExceptions.Count > 0)
            {
                throw new AggregateException(assertExceptions);
            }
        }

        [Fact]
        public void ExceptionCallStack_OutsideCatch()
        {
            List<Exception> assertExceptions = new();

            EventHandler<FirstChanceExceptionEventArgs> handler =
                (s, e) => FirstChanceExceptionCallback(e.Exception, assertExceptions, _outputHelper);

            AppDomain.CurrentDomain.FirstChanceException += handler;
            try
            {
                ThrowAndCatchEclipsingExceptionOutsideCatch();
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException -= handler;
            }

            if (assertExceptions.Count > 0)
            {
                throw new AggregateException(assertExceptions);
            }
        }

        private static readonly ThreadLocal<bool> ReentrancyTracker = new();

        private static void FirstChanceExceptionCallback(Exception thrownException, List<Exception> assertExceptions, ITestOutputHelper outputHelper)
        {
            if (ReentrancyTracker.Value)
                return;

            ReentrancyTracker.Value = true;

            outputHelper.WriteLine("Begin Observing Exception: " + thrownException.GetType());

            try
            {
                ValidateExceptionStackFrame(thrownException, outputHelper);
            }
            catch (XunitException ex)
            {
                assertExceptions.Add(ex);
            }
            finally
            {
                outputHelper.WriteLine("End Observing Exception: " + thrownException.GetType());

                ReentrancyTracker.Value = false;
            }
        }

        private static void ValidateExceptionStackFrame(Exception thrownException, ITestOutputHelper outputHelper)
        {
            StackTrace exceptionStackTrace = new(thrownException, fNeedFileInfo: false);

            // The stack trace of thrown exceptions is populated as the exception unwinds the
            // stack. In the case of observing the exception from the FirstChanceException event,
            // there is only one frame on the stack (the throwing frame). In order to get the
            // full call stack of the exception, get the current call stack of the thread and
            // filter out the call frames that are "above" the exception's throwing frame.
            StackFrame throwingFrame = null;
            foreach (StackFrame stackFrame in exceptionStackTrace.GetFrames())
            {
                if (null != stackFrame.GetMethod())
                {
                    throwingFrame = stackFrame;
                    break;
                }
            }

            Assert.NotNull(throwingFrame);

            outputHelper.WriteLine($"Throwing Frame: [{throwingFrame.GetMethod().Name}, 0x{GetOffset(throwingFrame):X}]");

            StackTrace threadStackTrace = new(fNeedFileInfo: false);
            ReadOnlySpan<StackFrame> threadStackFrames = threadStackTrace.GetFrames();
            int index = 0;

            outputHelper.WriteLine("Begin Checking Thread Frames:");
            while (index < threadStackFrames.Length)
            {
                StackFrame threadStackFrame = threadStackFrames[index];

                outputHelper.WriteLine($"- [{threadStackFrame.GetMethod().Name}, 0x{GetOffset(threadStackFrame):X}]");

                if (throwingFrame.GetMethod() == threadStackFrame.GetMethod() &&
                    GetOffset(throwingFrame) == GetOffset(threadStackFrame))
                {
                    break;
                }

                index++;
            }
            outputHelper.WriteLine("End Checking Thread Frames:");

            Assert.NotEqual(index, threadStackFrames.Length);
        }

        private static int GetOffset(StackFrame stackFrame)
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                return stackFrame.GetILOffset();
            }
            else
            {
                return stackFrame.GetNativeOffset();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowAndCatchEclipsingExceptionInsideCatch()
        {
            try
            {
                try
                {
                    throw new NotImplementedException();
                }
                catch (Exception ex)
                {
                    throw new Exception(null, ex);
                }
            }
            catch (Exception)
            {
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowAndCatchEclipsingExceptionOutsideCatch()
        {
            try
            {
                Exception caughtException = null;
                try
                {
                    throw new NotImplementedException();
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }

                throw new Exception(null, caughtException);
            }
            catch (Exception)
            {
            }
        }
    }
}