using System;
using System.Collections.Generic;
using System.Text;

namespace CustomTestingFramework
{
    
    public class MyAssertException : Exception
    {
        public MyAssertException(string message) : base(message) { }
    }

    
    public class TestContext
    {
        private readonly Dictionary<string, object> _data = new();
        public void Set(string key, object value) => _data[key] = value;
        public T Get<T>(string key) => (T)_data[key];
    }
}
