using System.Reflection;
namespace Sekiban.EventSourcing.Shared;

public interface IDependencyDefinition
{
    public virtual SekibanDependencyOptions GetSekibanDependencyOptions()
    {
        return new SekibanDependencyOptions(
            new RegisteredEventTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            new SekibanAggregateTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            GetCommandDependencies());
    }

    Assembly GetExecutingAssembly();

    /// <summary>
    ///     コントローラーに表示する集約
    /// </summary>
    /// <returns></returns>
    IEnumerable<Type> GetControllerAggregateTypes();


    /// <summary>
    ///     単集約用のプロジェクションリスト
    /// </summary>
    /// <returns></returns>
    public virtual IEnumerable<Type> GetSingleAggregateProjectionTypes()
    {
        return Enumerable.Empty<Type>();
    }

    /// <summary>
    ///     複数集約プロジェクションリスト
    ///     複数集約のプロジェクションの直接呼び出しはAPIからはできなくなりました。
    ///     クエリフィルタを使用してくたさい
    /// </summary>
    /// <returns></returns>
    public virtual IEnumerable<Type> GetMultipleAggregatesProjectionTypes()
    {
        return Enumerable.Empty<Type>();
    }

    public virtual IEnumerable<Type> GetAggregateListQueryFilterTypes()
    {
        return Enumerable.Empty<Type>();
    }
    public virtual IEnumerable<Type> GetAggregateQueryFilterTypes()
    {
        return Enumerable.Empty<Type>();
    }

    public virtual IEnumerable<Type> GetSingleAggregateProjectionListQueryFilterTypes()
    {
        return Enumerable.Empty<Type>();
    }
    public virtual IEnumerable<Type> GetSingleAggregateProjectionQueryFilterTypes()
    {
        return Enumerable.Empty<Type>();
    }

    public virtual IEnumerable<Type> GetProjectionQueryFilterTypes()
    {
        return Enumerable.Empty<Type>();
    }
    public virtual IEnumerable<Type> GetProjectionListQueryFilterTypes()
    {
        return Enumerable.Empty<Type>();
    }

    IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies();
}
