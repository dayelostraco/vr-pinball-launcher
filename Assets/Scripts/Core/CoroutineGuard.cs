using System;
using System.Collections;
using System.Collections.Generic;

namespace VRLauncher
{
    /// <summary>
    /// Observes failures raised after an asynchronous lifecycle operation has
    /// been scheduled, including nested enumerators. Ordinary engine yield
    /// instructions pass through unchanged; all owned enumerators are disposed.
    /// </summary>
    public static class CoroutineGuard
    {
        public static IEnumerator Run(IEnumerator operation, Action<Exception> failed)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (failed == null) throw new ArgumentNullException(nameof(failed));
            var stack = new Stack<IEnumerator>();
            stack.Push(operation);
            try
            {
                while (stack.Count > 0)
                {
                    bool moved = false;
                    object current = null;
                    Exception error = null;
                    try
                    {
                        moved = stack.Peek().MoveNext();
                        if (moved) current = stack.Peek().Current;
                    }
                    catch (Exception ex) { error = ex; }
                    if (error != null) { failed(error); yield break; }
                    if (!moved)
                    {
                        error = Dispose(stack.Pop());
                        if (error != null) { failed(error); yield break; }
                        continue;
                    }
                    if (current is IEnumerator nested) stack.Push(nested);
                    else yield return current;
                }
            }
            finally
            {
                // One broken cleanup must not prevent its parents from releasing
                // their resources. Report only after every owner is disposed.
                var errors = new List<Exception>();
                while (stack.Count > 0)
                {
                    Exception error = Dispose(stack.Pop());
                    if (error != null) errors.Add(error);
                }
                if (errors.Count > 0) failed(new AggregateException("Coroutine cleanup failed", errors));
            }
        }

        private static Exception Dispose(IEnumerator operation)
        {
            try { (operation as IDisposable)?.Dispose(); return null; }
            catch (Exception ex) { return ex; }
        }
    }
}
