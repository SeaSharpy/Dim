using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
public class DictionaryStack<TKey, TValue> where TKey : notnull
{
    private readonly Stack<Dictionary<TKey, TValue>> stack = new();

    public DictionaryStack()
    {
        stack.Push(new Dictionary<TKey, TValue>());
    }

    public void Push()
    {
        stack.Push(new Dictionary<TKey, TValue>());
    }
    public void Push(Dictionary<TKey, TValue> dict)
    {
        stack.Push(dict);
    }

    public Dictionary<TKey, TValue> Peek()
    {
        return stack.Peek();
    }

    public void Pop()
    {
        stack.Pop();
    }

    public void Set(TKey key, TValue value)
    {
        stack.Peek()[key] = value;
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        foreach (var dict in stack)
        {
            if (dict.TryGetValue(key, out value))
                return true;
        }
        value = default;
        return false;
    }
}

