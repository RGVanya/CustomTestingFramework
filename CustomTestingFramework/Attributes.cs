using System;
using System.Collections.Generic;
using System.Text;

namespace CustomTestingFramework
{
    // Маркер для класса, содержащего тесты
    [AttributeUsage(AttributeTargets.Class)]
    public class TestClassAttribute : Attribute { }

    // Маркер для тестового метода (с необязательным свойством Description)
    [AttributeUsage(AttributeTargets.Method)]
    public class TestMethodAttribute : Attribute
    {
        public string Description { get; }
        public TestMethodAttribute(string description = "") => Description = description;
    }

    // Параметризованные тесты
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TestCaseAttribute : Attribute
    {
        public object[] Parameters { get; }
        public TestCaseAttribute(params object[] parameters) => Parameters = parameters;
    }

    // Жизненный цикл теста
    [AttributeUsage(AttributeTargets.Method)] public class BeforeEachAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class AfterEachAttribute : Attribute { }
}
