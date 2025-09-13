using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Threading;
using SoftwareInstaller;

namespace SoftwareInstaller.Tests
{
    [TestClass]
    public class Form1ThreadingTest
    {
        [TestMethod]
        public void LoadListAsync_RunsOnBackgroundThread()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "dummy.exe"), string.Empty);

            int worker = -1;
            int ui = -1;

            var t = new Thread(() =>
            {
                ui = Thread.CurrentThread.ManagedThreadId;
                using var form = new Form1();
                form.SetSharePath(tempDir);
                form.LoadListAsync(id => worker = id).GetAwaiter().GetResult();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            Assert.AreNotEqual(ui, worker, "文件扫描应在后台线程运行");
        }
    }
}
