using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    public static class DirectoryInfoWPFExtensions
    {
        /// <summary>Проверка директории и вложенных каталогов в ней на наличие заблокированных файлов</summary>
        /// <param name="target"> директория для проверки</param>
        /// <returns>истина  - есть заблокированные файлы, лож - нет заблокированных файлов</returns>
        public static bool IsDirectoryHaveLockFile(this DirectoryInfo target) =>
            target.ThrowIfNotFound().IsLockFileInDirectory()
            || target
               .GetDirectories(searchOption: SearchOption.AllDirectories, searchPattern: ".")
               .Any(d => d.IsLockFileInDirectory());

        /// <summary>  Проверка директории и вложенных каталогов в ней на наличие заблокированных файлов  </summary>
        /// <param name="directory">директория для проверки</param>
        /// <returns>истина  - есть заблокированные файлы, лож - нет заблокированных файлов</returns>
        private static bool IsLockFileInDirectory(this DirectoryInfo directory) => directory.GetFiles().Any(file => file.IsLocked());

        public static IEnumerable<Process> EnumLockProcesses(this DirectoryInfo directory)
        {
            var locked_files = directory.GetFiles().Where(file => file.IsLocked()).ToList();
            var sub = directory
               .GetDirectories(searchOption: SearchOption.AllDirectories, searchPattern: ".")
               .Where(d => d.IsLockFileInDirectory()).SelectMany(d=>d.GetFiles().Where(f=>f.IsLocked())).ToArray();
            locked_files.AddRange(sub);

            return locked_files.Count > 0 ? locked_files.SelectMany(f => f.EnumLockProcesses()) : Enumerable.Empty<Process>();
        }
        public static Task WaitDirectoryLockAsync(this DirectoryInfo directory, CancellationToken Cancel = default) => directory
           .EnumLockProcesses()
           .Select(process => process.WaitAsync(Cancel))
           .WhenAll();

        public static async Task<bool> WaitDirectoryLockAsync(this DirectoryInfo directory, int Timeout, CancellationToken Cancel = default)
        {
            var processes = directory.EnumLockProcesses().Select(process => process.WaitAsync(Cancel));
            var process_wait = Task.WhenAll(processes);
            var delay_task = Task.Delay(Timeout, Cancel);
            var task = await Task.WhenAny(process_wait, delay_task).ConfigureAwait(false);
            return task != delay_task;
        }

        private static readonly WindowsIdentity __CurrentSystemUser = WindowsIdentity.GetCurrent();

        /// <summary>
        /// Рекурсивно проверяет есть ли директория и если ее нет - пробует создать
        /// </summary>
        /// <param name="dir">директория</param>
        /// <returns>истина если есть или удалось создать, лож если создать не удалось</returns>
        public static bool CheckExistsOrCreate(this DirectoryInfo dir)
        {
            if (Directory.Exists(dir.FullName)) return true;
            else if (!(dir.Parent is null) && dir.Parent.CheckExistsOrCreate())
            {
                if (!dir.Parent.CanAccessToDirectory(FileSystemRights.CreateDirectories)) return false;
                Directory.CreateDirectory(dir.FullName);
                return true;
            }
            return false;
        }
        /// <summary>
        /// Проверяет право просмотреть директорию в списке
        /// </summary>
        /// <param name="dir">директория</param>
        /// <returns></returns>
        public static bool CanAccessToDirectoryListItems(this DirectoryInfo dir) => dir.CanAccessToDirectory(FileSystemRights.ListDirectory);

        /// <summary>
        /// проверяет право на директорию в соответствии с заданными правами
        /// </summary>
        /// <param name="dir">директория</param>
        /// <param name="AccessRight">искомые права (по умолчанию права на изменение)</param>
        /// <returns></returns>
        public static bool CanAccessToDirectory(this DirectoryInfo dir, FileSystemRights AccessRight = FileSystemRights.Modify)
            => dir.CanAccessToDirectory(__CurrentSystemUser, AccessRight);
        /// <summary>
        /// хранит список директорий с заблокированным доступом на любые права
        /// </summary>
        private static readonly ConcurrentDictionary<int, bool> __BadDirectories = new();
        /// <summary>
        /// проверяет права на директорию в соответствии с заданными правами и уровнем доступа пользователя
        /// </summary>
        /// <param name="dir">директория</param>
        /// <param name="user">пользователь WindowsIdentity </param>
        /// <param name="AccessRight">уровень доступа (по умолчанию права на изменение)</param>
        /// <returns></returns>
        public static bool CanAccessToDirectory(this DirectoryInfo dir, WindowsIdentity user, FileSystemRights AccessRight = FileSystemRights.Modify)
        {
            if (dir is null) throw new ArgumentNullException(nameof(dir));
            if (!dir.Exists) throw new InvalidOperationException($"Директория {dir.FullName} не существует");
            if (user is null) throw new ArgumentNullException(nameof(user));
            if (user.Groups is null) throw new ArgumentException("В идетнификаторе пользователя отсутствует ссылка на группы", nameof(user));

            if (__BadDirectories.ContainsKey(dir.FullName.GetHashCode()))
                return false;

            AuthorizationRuleCollection rules;
            try
            {
                rules = dir.GetAccessControl(AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier));
            }
            catch (UnauthorizedAccessException)
            {
                __BadDirectories[dir.FullName.GetHashCode()] = false;
                Trace.WriteLine($"CanAccessToDirectory: Отсутствует разрешение на просмотр разрешений каталога {dir.FullName}");
                return false;
            }
            catch (InvalidOperationException)
            {
                Trace.WriteLine($"CanAccessToDirectory: Ошибка чтения каталога {dir.FullName}");
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                Trace.WriteLine($"CanAccessToDirectory: Директория не существует {dir.FullName}");
                return false;
            }
            catch (Exception)
            {
                Trace.WriteLine($"CanAccessToDirectory: неизвестная ошибка {dir.FullName}");
                return false;
            }

            var allow = false;
            var deny = false;

            #region Проверка прав для локальных папок

            var access_local_rights = new List<FileSystemAccessRule>(rules.Count);

            foreach (FileSystemAccessRule rule in rules)
            {

                var sid = (SecurityIdentifier)rule.IdentityReference;
                if ((!sid.IsAccountSid() || user.User != sid) && (sid.IsAccountSid() || !user.Groups.Contains(sid))) continue;
                var rights = MapGenericRightsToFileSystemRights(rule.FileSystemRights); //Преобразование составного ключа
                if (((int)rule.FileSystemRights != -1) && (rights & AccessRight) == AccessRight)
                    access_local_rights.Add(rule);

            }

            foreach (var rule in access_local_rights)
                switch (rule.AccessControlType)
                {
                    case AccessControlType.Allow:
                        allow = true;
                        break;
                    case AccessControlType.Deny:
                        deny = true;
                        break;
                }

            var local_access = allow && !deny;

            #endregion

            #region проверка прав для серверных папок
            allow = false;
            deny = false;

            var access_server_rights = rules.OfType<FileSystemAccessRule>()
               .Where(rule => user.Groups.Contains(rule.IdentityReference) && (int)rule.FileSystemRights != -1 && (rule.FileSystemRights & AccessRight) == AccessRight).ToArray();

            foreach (var rule in access_server_rights)
                switch (rule.AccessControlType)
                {
                    case AccessControlType.Allow:
                        allow = true;
                        break;
                    case AccessControlType.Deny:
                        deny = true;
                        break;
                }

            var server_access = allow && !deny;

            #endregion

            #region Финальная проверка прав на чтение

            var look_dir = false;
            if (AccessRight == FileSystemRights.ListDirectory)
                try
                {
                    dir.GetDirectories();
                    look_dir = true;
                }
                catch (UnauthorizedAccessException) { }

            #endregion

            return local_access || server_access || look_dir;
        }

        /// <summary>Составные ключи прав доступа</summary>
        [Flags]
        private enum GenericRights : uint
        {
            Read = 0x80000000,
            Write = 0x40000000,
            Execute = 0x20000000,
            All = 0x10000000
        }
        /// <summary>Преобразование прав доступа из составных ключей</summary>
        /// <param name="OriginalRights"></param>
        /// <returns></returns>
        private static FileSystemRights MapGenericRightsToFileSystemRights(FileSystemRights OriginalRights)
        {
            var mapped_rights = new FileSystemRights();
            var was_number = false;
            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.Execute)))
            {
                mapped_rights = mapped_rights | FileSystemRights.ExecuteFile | FileSystemRights.ReadPermissions | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;
                was_number = true;
            }

            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.Read)))
            {
                mapped_rights = mapped_rights | FileSystemRights.ReadAttributes | FileSystemRights.ReadData | FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize;
                was_number = true;
            }
            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.Write)))
            {
                mapped_rights = mapped_rights | FileSystemRights.AppendData | FileSystemRights.WriteAttributes | FileSystemRights.WriteData | FileSystemRights.WriteExtendedAttributes | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize;
                was_number = true;
            }
            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.All)))
            {
                mapped_rights |= FileSystemRights.FullControl;
                was_number = true;
            }

            return was_number ? mapped_rights : OriginalRights;
        }

        /// <summary>Получает список всех вложенных директорий</summary>
        /// <param name="ParentDirectory">родительская директория</param>
        /// <returns></returns>
        public static IEnumerable<DirectoryInfo> GetAllSubDirectory(this DirectoryInfo ParentDirectory) =>
            CanAccessToDirectoryListItems(ParentDirectory)
                ? ParentDirectory
                   .GetDirectories(searchOption: SearchOption.AllDirectories, searchPattern: ".")
                   .Where(dir => dir.CanAccessToDirectory(FileSystemRights.ListDirectory))
                : Array.Empty<DirectoryInfo>();

        /// <summary>Получает список всех вложенных директорий на основании прав доступа</summary>
        /// <param name="ParentDirectory">родительская директория</param>
        /// <param name="rights">право доступа</param>
        /// <returns></returns>
        public static IEnumerable<DirectoryInfo> GetAllSubDirectory(this DirectoryInfo ParentDirectory, FileSystemRights rights) =>
            ParentDirectory.CanAccessToDirectory(rights)
                ? ParentDirectory.GetDirectoryInfo(rights)
                : Array.Empty<DirectoryInfo>();

        private static IEnumerable<DirectoryInfo> GetDirectoryInfo(this DirectoryInfo ParentDirectory, FileSystemRights rights)
        {
            if (!ParentDirectory.CanAccessToDirectory(rights)) yield break;
            foreach (var directory in ParentDirectory.GetDirectories())
                if (directory.CanAccessToDirectory(rights))
                {
                    yield return directory;
                    foreach (var sub_dir in directory.GetDirectoryInfo(rights))
                        yield return sub_dir;
                }

        }

        /// <summary>Получает список всех вложенных директорий на основании прав доступа</summary>
        /// <param name="ParentDirectory">родительская директория</param>
        /// <param name="rights">право доступа</param>
        /// <param name="Cancel">Флаг отмены асинхронной операции</param>
        /// <returns></returns>
        public static async Task<IEnumerable<DirectoryInfo>> GetAllSubDirectoryAsync(
            this DirectoryInfo ParentDirectory,
            FileSystemRights rights = FileSystemRights.ListDirectory,
            CancellationToken Cancel = default)
        {
            Cancel.ThrowIfCancellationRequested();
            return !ParentDirectory.CanAccessToDirectory(rights)
                ? Array.Empty<DirectoryInfo>()
                : await ParentDirectory.GetDirectoryInfoAsync(rights, Cancel).ConfigureAwait(false);
        }

        private static async Task<IEnumerable<DirectoryInfo>> GetDirectoryInfoAsync(
            this DirectoryInfo ParentDirectory,
            FileSystemRights rights,
            CancellationToken Cancel = default)
        {
            Cancel.ThrowIfCancellationRequested();
            if (!ParentDirectory.CanAccessToDirectory(FileSystemRights.ListDirectory))
                return Enumerable.Empty<DirectoryInfo>();

            await Task.Yield().ConfigureAwait(false);

            var dirs = new List<DirectoryInfo>();
            DirectoryInfo[] directories;
            try
            {
                directories = ParentDirectory.GetDirectories();
            }
            catch (UnauthorizedAccessException)
            {
                return Enumerable.Empty<DirectoryInfo>();
            }

            foreach (var dir in directories)
                if (dir.CanAccessToDirectory(rights))
                {
                    Cancel.ThrowIfCancellationRequested();

                    dirs.Add(dir);

                    dirs.AddRange(await dir.GetDirectoryInfoAsync(rights, Cancel));
                }
            return dirs;
        }

        #region Sub extensions

        /// <summary>Проверка, что директория существует</summary>
        /// <param name="Dir">Проверяемая директория</param>
        /// <param name="Message">Сообщение, добавляемое в исключение, если директория не найдена</param>
        /// <returns>Директория, гарантированно существующая</returns>
        /// <exception cref="T:System.IO.DirectoryNotFoundException">В случае если <paramref name="Dir"/> не существует.</exception>
        internal static DirectoryInfo ThrowIfNotFound(this DirectoryInfo Dir, string Message = null)
        {
            var dir = Dir.NotNull("Отсутствует ссылка на директории");
            return !dir.Exists ? throw new DirectoryNotFoundException(Message ?? $"Директория {dir.FullName} не найдена") : dir;
        }
        /// <summary>Проверка на пустую ссылку</summary>
        /// <typeparam name="T">Тип проверяемого объекта</typeparam>
        /// <param name="obj">Проверяемое значение</param>
        /// <param name="Message">Сообщение ошибки</param>
        /// <returns>Значение, точно не являющееся пустой ссылкой</returns>
        /// <exception cref="InvalidOperationException">В случае если переданное значение <paramref name="obj"/> == null</exception>

        internal static T NotNull<T>(this T? obj, string? Message = null) where T : class =>
            obj ?? throw new InvalidOperationException(Message ?? "Пустая ссылка на объект");

        /// <summary>Продолжить в пуле потоков</summary>
        /// <param name="LockContext">Если истина, то продолжение будет выполнено в том же потоке, если ложь - то в пуле потоков</param>
        internal static YieldAwaitableThreadPool ConfigureAwait(
            this YieldAwaitable _,
            bool LockContext)
        {
            return new(in LockContext);
        }
        #endregion
    }
}
