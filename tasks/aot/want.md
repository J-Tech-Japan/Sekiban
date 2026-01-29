AOTで ResultでないReturnのドメインを定義したい。

そのために、プロジェクト分けも必要と思われる。
Sekiban.Dcb.Core.Model だけではダメで、
Sekiban.Dcb.WithoutResult.Model も必要そう
かつ、参照順位の工夫が必要そうなので設計して

/Users/tomohisa/dev/GitHub/Sekiban-dcb/dcb/src/Sekiban.Dcb.WithoutResult/MultiProjections/IMultiProjector.cs
の
    static abstract T Project(
        T payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold);
のかたちで書きたい

たぶん、今よりもさらに最適化して、AOTでWithoutResultの形式でも、場合によっては、WithResultの形式で書けることも必要

/Users/tomohisa/dev/GitHub/Sekiban-dcb/dcb/src/Sekiban.Dcb.WithoutResult/Queries/IMultiProjectionListQuery.cs

のかたちで
    /// <summary>
    ///     Filter the projection to get the items
    /// </summary>
    /// <param name="projector">The multi-projector state</param>
    /// <param name="query">The query instance</param>
    /// <param name="context">The query context</param>
    /// <returns>The filtered items</returns>
    static abstract IEnumerable<TOutput> HandleFilter(
        TMultiProjector projector,
        TQuery query,
        IQueryContext context);

    /// <summary>
    ///     Sort the filtered items
    /// </summary>
    /// <param name="filteredList">The filtered items</param>
    /// <param name="query">The query instance</param>
    /// <param name="context">The query context</param>
    /// <returns>The sorted items</returns>
    static abstract IEnumerable<TOutput> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
のかたちで書けることも必要


/Users/tomohisa/dev/GitHub/Sekiban-dcb/dcb/src/Sekiban.Dcb.Core.Model/Tags/ITagProjector.cs
はWithresult もWithoutResultも同じなので良さそう