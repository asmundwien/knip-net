namespace CatD.D16;

using System;

[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class UsedNamedTypeParameterAttribute : Attribute { }
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class UnusedNamedTypeParameterAttribute : Attribute { }

[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class UsedDelegateTypeParameterAttribute : Attribute { }
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class UnusedDelegateTypeParameterAttribute : Attribute { }

[AttributeUsage(AttributeTargets.ReturnValue)]
public sealed class UsedDelegateReturnAttribute : Attribute { }
[AttributeUsage(AttributeTargets.ReturnValue)]
public sealed class UnusedDelegateReturnAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UsedDelegateParameterAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UnusedDelegateParameterAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UsedPrimaryParameterAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UnusedPrimaryParameterAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UsedIndexerParameterAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UnusedIndexerParameterAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field)]
public sealed class UsedBackingFieldAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Field)]
public sealed class UnusedBackingFieldAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class UsedEventAccessorAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Method)]
public sealed class UnusedEventAccessorAttribute : Attribute { }

public sealed class Runner
{
    [return: UsedDelegateReturn]
    private delegate int Callback<[UsedDelegateTypeParameter] T>([UsedDelegateParameter] T value);

    private sealed class Box<[UsedNamedTypeParameter] T>
    {
    }

    private sealed class Worker([UsedPrimaryParameter] int value)
    {
        public int Value => value;
    }

    [field: UsedBackingField]
    private int Stored { get; set; }

    private int this[[UsedIndexerParameter] int index] => index;

    private event Action Changed
    {
        [UsedEventAccessor]
        add { }
        remove { }
    }

    public void ConfigureServices()
    {
        Callback<int> callback = value => value;
        _ = callback(1);
        _ = new Box<int>();
        _ = new Worker(1).Value;
        Stored = 1;
        _ = Stored;
        _ = this[0];
        Changed += static () => { };
    }
}
