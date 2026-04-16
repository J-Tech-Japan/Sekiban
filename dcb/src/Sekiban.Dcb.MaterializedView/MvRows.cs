using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace Sekiban.Dcb.MaterializedView;

public interface IMvRow
{
    int ColumnCount { get; }
    IReadOnlyList<string> ColumnNames { get; }
    bool IsNull(string columnName);
    Guid GetGuid(string columnName);
    string GetString(string columnName);
    int GetInt32(string columnName);
    long GetInt64(string columnName);
    decimal GetDecimal(string columnName);
    double GetDouble(string columnName);
    bool GetBoolean(string columnName);
    DateTimeOffset GetDateTimeOffset(string columnName);
    byte[] GetBytes(string columnName);
    Guid? GetGuidOrNull(string columnName);
    string? GetStringOrNull(string columnName);
    int? GetInt32OrNull(string columnName);
    decimal? GetDecimalOrNull(string columnName);
    DateTimeOffset? GetDateTimeOffsetOrNull(string columnName);
    T GetAs<T>(string columnName);
    string ToJson();
}

public interface IMvRowSet : IReadOnlyList<IMvRow>
{
    IReadOnlyList<string> ColumnNames { get; }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class MvColumnAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public static class MvRowMapper<T> where T : class
{
    private static readonly ConcurrentDictionary<Type, Func<IMvRow, T>> Cache = new();

    public static T MapFrom(IMvRow row) => Cache.GetOrAdd(typeof(T), _ => BuildMapper())(row);

    public static IReadOnlyList<T> MapAll(IMvRowSet set) => set.Select(MapFrom).ToList();

    private static Func<IMvRow, T> BuildMapper()
    {
        var type = typeof(T);
        var rowParameter = Expression.Parameter(typeof(IMvRow), "row");
        var ctor = GetPreferredConstructor(type);

        if (ctor is null)
        {
            throw new InvalidOperationException($"Type {type.FullName} must expose a public constructor.");
        }

        var ctorArguments = ctor.GetParameters()
            .Select(parameter => CreateColumnReadExpression(
                rowParameter,
                ResolveColumnName(type, parameter),
                parameter.ParameterType))
            .ToArray();

        Expression body = Expression.New(ctor, ctorArguments);

        var writableProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is not null &&
                               ctor.GetParameters().All(parameter =>
                                   !string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (writableProperties.Length > 0)
        {
            body = Expression.MemberInit(
                (NewExpression)body,
                writableProperties.Select(property =>
                    Expression.Bind(
                        property,
                        CreateColumnReadExpression(rowParameter, ResolveColumnName(property), property.PropertyType))));
        }

        return Expression.Lambda<Func<IMvRow, T>>(body, rowParameter).Compile();
    }

    private static Expression CreateColumnReadExpression(ParameterExpression rowParameter, string columnName, Type targetType)
    {
        var method = typeof(MvRowValueReader).GetMethod(nameof(MvRowValueReader.ReadValue), BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(targetType);
        return Expression.Call(method, rowParameter, Expression.Constant(columnName));
    }

    private static ConstructorInfo? GetPreferredConstructor(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length == 0)
        {
            return null;
        }

        var ranked = constructors
            .Select(constructor => new
            {
                Constructor = constructor,
                ReadOnlyMatches = CountReadOnlyPropertyMatches(type, constructor)
            })
            .OrderByDescending(candidate => candidate.ReadOnlyMatches)
            .ThenByDescending(candidate => candidate.Constructor.GetParameters().Length)
            .ToList();

        if (ranked[0].ReadOnlyMatches > 0)
        {
            return ranked[0].Constructor;
        }

        return constructors.FirstOrDefault(constructor => constructor.GetParameters().Length == 0) ?? ranked[0].Constructor;
    }

    private static int CountReadOnlyPropertyMatches(Type type, ConstructorInfo constructor) =>
        constructor.GetParameters().Count(parameter =>
        {
            var property = type.GetProperty(
                parameter.Name ?? string.Empty,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return property?.SetMethod is null;
        });

    private static string ResolveColumnName(PropertyInfo property) =>
        property.GetCustomAttribute<MvColumnAttribute>()?.Name ?? ToSnakeCase(property.Name);

    private static string ResolveColumnName(Type targetType, ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttribute<MvColumnAttribute>();
        if (attribute is not null)
        {
            return attribute.Name;
        }

        var property = targetType.GetProperty(
            parameter.Name ?? string.Empty,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property?.GetCustomAttribute<MvColumnAttribute>()?.Name ?? ToSnakeCase(parameter.Name ?? string.Empty);
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current))
            {
                if (i > 0)
                {
                    chars.Add('_');
                }
                chars.Add(char.ToLowerInvariant(current));
            }
            else
            {
                chars.Add(current);
            }
        }

        return new string(chars.ToArray());
    }
}

public static class MvRowValueReader
{
    public static TValue ReadValue<TValue>(IMvRow row, string columnName) => row.GetAs<TValue>(columnName);
}

public static class MvApplyContextExtensions
{
    public static async Task<T?> QuerySingleOrDefaultAsync<T>(
        this IMvApplyContext ctx,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var row = await ctx.QuerySingleOrDefaultRowAsync(sql, param, cancellationToken).ConfigureAwait(false);
        return row is null ? null : MvRowMapper<T>.MapFrom(row);
    }

    public static async Task<IReadOnlyList<T>> QueryAsync<T>(
        this IMvApplyContext ctx,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var rows = await ctx.QueryRowsAsync(sql, param, cancellationToken).ConfigureAwait(false);
        return MvRowMapper<T>.MapAll(rows);
    }
}

public static class MvRowValueConverter
{
    public static T ConvertValue<T>(object? value)
    {
        if (value is null or DBNull)
        {
            return ConvertNullValue<T>();
        }

        if (value is T typed)
        {
            return typed;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (TryConvertSpecialValue(value, targetType, out var converted))
        {
            return (T)converted;
        }

        return (T)Convert.ChangeType(value, targetType);
    }

    private static T ConvertNullValue<T>()
    {
        if (IsNullable(typeof(T)))
        {
            return default!;
        }

        throw new InvalidOperationException($"Column value is null but target type {typeof(T).Name} is not nullable.");
    }

    private static bool TryConvertSpecialValue(object value, Type targetType, out object converted)
    {
        if (targetType.IsEnum)
        {
            converted = Enum.Parse(targetType, value.ToString()!, ignoreCase: true);
            return true;
        }

        if (targetType == typeof(Guid))
        {
            converted = value is Guid guid ? guid : Guid.Parse(value.ToString()!);
            return true;
        }

        if (targetType == typeof(DateTimeOffset) && TryConvertDateTimeOffset(value, out var dateTimeOffset))
        {
            converted = dateTimeOffset;
            return true;
        }

        if (targetType == typeof(byte[]) && value is byte[] bytes)
        {
            converted = bytes;
            return true;
        }

        if (targetType == typeof(string))
        {
            converted = value.ToString()!;
            return true;
        }

        if (value is JsonElement jsonElement)
        {
            converted = jsonElement.Deserialize(targetType)!;
            return true;
        }

        converted = default!;
        return false;
    }

    private static bool TryConvertDateTimeOffset(object value, out DateTimeOffset dateTimeOffset)
    {
        if (value is DateTimeOffset dto)
        {
            dateTimeOffset = dto;
            return true;
        }

        if (value is DateTime dateTime)
        {
            dateTimeOffset = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
            return true;
        }

        dateTimeOffset = default;
        return false;
    }

    public static bool IsNullable(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}
