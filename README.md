# DirectoryExtensions
Расширения для директорий

## Locked Directory

### Быстрый старт:

`Install-LockAccessDirectoryExtensions -Version 1.0.0`

#### Для проверки блокирующих файлов в директории

```C#
var dir = new DirectoryInfo(DirFullPath);
if (dir.Exists && dir.IsDirectoryHaveLockFile())
{
    ...
}
```
#### Для  ожидания разблокировки директории

```C#
var dir = new DirectoryInfo(DirFullPath);
await dir.WaitDirectoryLockAsync();
```
