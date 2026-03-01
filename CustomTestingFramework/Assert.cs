using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace CustomTestingFramework
{
    public static class Assert
    {
        public static void AreEqual(object exp, object act)
        {
            if (!Equals(exp, act)) throw new MyAssertException($"Expected {exp}, but got {act}");
        }
        public static void IsTrue(bool condition)
        {
            if (!condition) throw new MyAssertException("Value is not True");
        }
        public static void IsFalse(bool condition)
        {
            if (condition) throw new MyAssertException("Value is not False");
        }
        public static void IsNull(object obj)
        {
            if (obj != null) throw new MyAssertException("Object is not Null");
        }
        public static void IsNotNull(object obj)
        {
            if (obj == null) throw new MyAssertException("Object is Null");
        }
        public static void StringContains(string sub, string full)
        {
            if (full == null || !full.Contains(sub)) throw new MyAssertException($"'{full}' doesn't contain '{sub}'");
        }
        public static void IsEmpty(IEnumerable collection)
        {
            if (collection.GetEnumerator().MoveNext()) throw new MyAssertException("Collection is not empty");
        }
        public static void IsGreaterThan(int val, int limit)
        {
            if (val <= limit) throw new MyAssertException($"{val} is not greater than {limit}");
        }
        public static void AreSame(object a, object b)
        {
            if (!ReferenceEquals(a, b)) throw new MyAssertException("References are not same");
        }
        public static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new MyAssertException($"Expected exception {typeof(T).Name} was not thrown");
        }
    }
}
