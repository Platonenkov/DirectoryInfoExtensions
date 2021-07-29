namespace DirectoryInfoExtensions
{
    /// <summary>Размеры данных</summary>
    internal static class DataLength
    {
        /// <summary>Размеры данных в байтах</summary>
        public static class Bytes
        {
            /// <summary>1 Байт</summary>
            public const int B = 1;
            /// <summary>1 килобайт в байтах</summary>
            public const int kB = 0x400 * B;
            /// <summary>1 мегабайт в байтах</summary>
            public const int MB = 0x400 * kB;
            /// <summary>1 гигабайт в байтах</summary>
            public const int GB = 0x400 * MB;
            /// <summary>1 террарий в байтах</summary>
            public const long TB = 1024L * GB;

            /// <summary>Получить имена значений количества байт с приставками "B", "kB", "MB", "GB", "TB"</summary>
            /// <returns>"B", "kB", "MB", "GB", "TB"</returns>
            public static string[] GetDataNames() => new[] { "B", "kB", "MB", "GB", "TB" };

            /// <summary>Получить русскоязычные имена значений количества байт с приставками "Б", "кБ", "МБ", "ГБ", "ТБ"</summary>
            /// <returns>"Б", "кБ", "МБ", "ГБ", "ТБ"</returns>
            public static string[] GetDataNamesRu() => new[] { "Б", "кБ", "МБ", "ГБ", "ТБ" };
        }
    }
}
