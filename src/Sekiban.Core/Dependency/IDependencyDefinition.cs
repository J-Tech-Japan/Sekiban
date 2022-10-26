using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query;
using System.Reflection;
namespace Sekiban.Core.Dependency;

public interface IDependencyDefinition
{

    public bool MakeSimpleAggregateListQueryFilter { get; }
    public bool MakeSimpleSingleAggregateProjectionListQueryFilter { get; }
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

    public virtual IEnumerable<Type> GetSimpleAggregateListQueryFilterTypes()
    {
        if (MakeSimpleAggregateListQueryFilter)
        {
            var baseSimpleAggregateListQueryFilterType = typeof(SimpleAggregateListQueryFilter<>);
            return GetControllerAggregateTypes().Select(m => baseSimpleAggregateListQueryFilterType.MakeGenericType(m));
        }
        return Enumerable.Empty<Type>();
    }
    public virtual IEnumerable<Type> GetSimpleSingleAggregateProjectionListQueryFilterTypes()
    {
        if (MakeSimpleSingleAggregateProjectionListQueryFilter)
        {
            var baseSimpleAggregateListQueryFilterType = typeof(SimpleSingleAggregateProjectionListQueryFilter<,,>);
            return GetSingleAggregateProjectionTypes()
                .Select(
                    m =>
                    {
                        var baseType = m.BaseType;
                        if (baseType == null)
                        {
                            throw new SekibanQueryFilterGenerationError();
                        }
                        var aggregateType = baseType.GenericTypeArguments[0];
                        var projectionType = baseType.GenericTypeArguments[1];
                        var contentsType = baseType.GenericTypeArguments[2];
                        return baseSimpleAggregateListQueryFilterType.MakeGenericType(aggregateType, projectionType, contentsType);
                    });
        }
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
