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

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TestCaseSourceAttribute : Attribute
    {
        public string SourceName { get; }
        public TestCaseSourceAttribute(string sourceName) => SourceName = sourceName;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class CategoryAttribute : Attribute
    {
        public string Name { get; }
        public CategoryAttribute(string name) => Name = name;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class PriorityAttribute : Attribute
    {
        public int Level { get; }
        public PriorityAttribute(int level) => Level = level;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class AuthorAttribute : Attribute
    {
        public string Name { get; }
        public AuthorAttribute(string name) => Name = name;
    }

    // Жизненный цикл теста
    [AttributeUsage(AttributeTargets.Method)] public class BeforeEachAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class AfterEachAttribute : Attribute { }

    // Атрибут для ограничения времени выполнения теста.
    [AttributeUsage(AttributeTargets.Method)]
    public class TimeoutAttribute : Attribute
    {
        public int Milliseconds { get; }
        public TimeoutAttribute(int milliseconds) => Milliseconds = milliseconds;
    }

    // Атрибут для управления параллельным выполнением на уровне класса.
    // Позволяет включать/отключать параллелизм методов внутри тестового класса.
    [AttributeUsage(AttributeTargets.Class)]
    public class ParallelizableAttribute : Attribute { }
}
