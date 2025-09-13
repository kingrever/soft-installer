using System;
using System.Windows.Forms;

namespace SoftwareInstaller
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            try
            {
                Application.Run(new Form1());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "启动错误");
            }

        }
    }
}
