namespace PluginOrigin.Lib;

public sealed class Targets
{
    public static void TestReflection() { }
    public static void ProductionReflection() { }
    public static int TestScanning() => 1;
    public static int ProductionScanning() => 1;
    public static int TestBlazor() => 1;
    public static int ProductionBlazor() => 1;
    public static void TestAspNet() { }
    public static void ProductionAspNet() { }
    public static void NeverUsed() { }
}

public sealed class TestSerializationDto
{
    public int Value { get; set; }
}

public sealed class ProductionSerializationDto
{
    public int Value { get; set; }
}

public sealed class TestRegisteredService
{
    private readonly int _value = Targets.TestScanning();
}

public sealed class ProductionRegisteredService
{
    private readonly int _value = Targets.ProductionScanning();
}

public sealed class TestMiddleware
{
    public void Invoke() => Targets.TestAspNet();
}

public sealed class ProductionMiddleware
{
    public void Invoke() => Targets.ProductionAspNet();
}
