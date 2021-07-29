using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace TestConsoleLockDirectory
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var dir = new DirectoryInfo("Test");
            if (!dir.Exists)
                dir.Create();

            await CheckAndWait(dir);

            for (var i = 0; i < 3; i++)
                await CreateFile($"Test\\Test_{i}\\Test_{i}.csv");

            Console.WriteLine("File will open after 5 second");
            await Task.Delay(TimeSpan.FromSeconds(5));
            await CheckAndWait(dir);



        }

        static async Task CreateFile(string fileName)
        {
            var file = new FileInfo(fileName);
            if(!file.Directory.Exists)
                file.Directory.Create();
            await File.WriteAllTextAsync(file.FullName, "1;2;3;4", Encoding.UTF8);

            new Process { StartInfo = new ProcessStartInfo(file.FullName) { UseShellExecute = true } }.Start();

        }
        static async Task CheckAndWait(DirectoryInfo directory)
        {
            if (directory.IsDirectoryHaveLockFile())
            {
                Console.WriteLine();
                Console.WriteLine($"Directory is locked {directory.FullName} by processes:");
                foreach (var lock_process in directory.EnumLockProcesses())
                {
                    Console.WriteLine($"{lock_process.ProcessName}");
                }

                Console.WriteLine();
                Console.WriteLine("Wait while directory is lock");
                await directory.WaitDirectoryLockAsync();
                Console.WriteLine("directory was unlocked");
                Console.WriteLine();
                Console.WriteLine("Press any key to close");
                Console.ReadKey();
            }

        }
    }
}
