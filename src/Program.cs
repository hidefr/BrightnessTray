using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace BrightnessTray
{
    static class Program
    {
        private const string MutexName = @"Local\BrightnessTray.SingleInstance";
        private const string EventName = @"Local\BrightnessTray.Show";

        private static Mutex _singleInstanceMutex;
        private static EventWaitHandle _showEvent;
        private static RegisteredWaitHandle _registeredWait;

        [STAThread]
        static void Main(string[] args)
        {
            // Set up global crash logging
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                LogError(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                LogError(e.ExceptionObject as Exception);
            };

            bool createdNew;
            try
            {
                _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                // The previous instance terminated without releasing the mutex.
                // We now own the mutex, so this is the active single instance.
                createdNew = true;
            }
            catch
            {
                createdNew = false;
            }

            if (!createdNew)
            {
                // Another instance is already running -> Signal it to show the panel and exit
                try
                {
                    EventWaitHandle ev;
                    if (EventWaitHandle.TryOpenExisting(EventName, out ev))
                    {
                        ev.Set();
                        ev.Dispose();
                    }
                }
                catch { }
                return;
            }

            bool startSilent = false;
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase))
                    {
                        startSilent = true;
                        break;
                    }
                }
            }

            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            TrayApp app = new TrayApp(startSilent);

            if (_showEvent != null)
            {
                _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                    _showEvent,
                    delegate(object state, bool timedOut)
                    {
                        if (!timedOut)
                        {
                            app.Post(delegate
                            {
                                app.ShowFlyout();
                            });
                        }
                    },
                    null,
                    -1,
                    false
                );
            }

            Application.Run(app);

            // Clean up
            if (_registeredWait != null)
            {
                _registeredWait.Unregister(null);
            }
            if (_showEvent != null)
            {
                _showEvent.Dispose();
            }
            if (_singleInstanceMutex != null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
        }

        private static void LogError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string logPath = Settings.GetErrorLogFilePath();
                string entry = string.Format(
                    "[{0:yyyy-MM-dd HH:mm:ss}] Unhandled Exception: {1}\r\nStack Trace:\r\n{2}\r\n\r\n",
                    DateTime.Now,
                    ex.Message,
                    ex.StackTrace
                );
                File.AppendAllText(logPath, entry);
            }
            catch { }
        }
    }
}
