namespace MyTestFramework
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

    // [ДОБАВЛЕНО] Атрибут для ограничения времени выполнения теста.
    // Если тест не завершился за указанное количество миллисекунд — он принудительно прерывается.
    [AttributeUsage(AttributeTargets.Method)]
    public class TimeoutAttribute : Attribute
    {
        public int Milliseconds { get; }
        public TimeoutAttribute(int milliseconds) => Milliseconds = milliseconds;
    }

    // [ДОБАВЛЕНО] Атрибут для управления параллельным выполнением на уровне класса.
    // Позволяет включать/отключать параллелизм методов внутри тестового класса.
    [AttributeUsage(AttributeTargets.Class)]
    public class ParallelizableAttribute : Attribute { }
}
