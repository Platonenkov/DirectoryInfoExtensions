using System.Collections.Generic;

namespace DirectoryInfoExtensions.Trees
{
    /// <summary>Элемент дву-связного дерева</summary>
    /// <typeparam name="T">Тип узла дерева</typeparam>
    public interface ITreeNode<out T> where T : class, ITreeNode<T>
    {
        /// <summary>Родительский узел</summary>
        T Parent { get; }

        /// <summary>Дочерние узлы</summary>
        IEnumerable<T> Childs { get; }
    }

    /// <summary>Элемент дву-связного дерева</summary>
    /// <typeparam name="T">Тип узла дерева</typeparam>
    /// <typeparam name="TValue">Тип значения</typeparam>
    public interface ITreeNode<out T, out TValue> : ITreeNode<T> where T : class, ITreeNode<T, TValue>
    {
        /// <summary>Значение узла</summary>
        TValue Value { get; }
    }
}
