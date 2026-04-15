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
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

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
        var method = typeof(MvRowMapper<T>).GetMethod(nameof(ReadValue), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(targetType);
        return Expression.Call(method, rowParameter, Expression.Constant(columnName));
    }

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

    private static TValue ReadValue<TValue>(IMvRow row, string columnName) => row.GetAs<TValue>(columnName);

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
        if (value is null || value is DBNull)
        {
            if (IsNullable(typeof(T)))
            {
                return default!;
            }

            throw new InvalidOperationException($"Column value is null but target type {typeof(T).Name} is not nullable.");
        }

        if (value is T typed)
        {
            return typed;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType.IsEnum)
        {
            return (T)Enum.Parse(targetType, value.ToString()!, ignoreCase: true);
        }

        if (targetType == typeof(Guid))
        {
            return (T)(object)(value is Guid guid ? guid : Guid.Parse(value.ToString()!));
        }

        if (targetType == typeof(DateTimeOffset))
        {
            if (value is DateTimeOffset dateTimeOffset)
            {
                return (T)(object)dateTimeOffset;
            }

            if (value is DateTime dateTime)
            {
                return (T)(object)new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
            }
        }

        if (targetType == typeof(byte[]) && value is byte[] bytes)
        {
            return (T)(object)bytes;
        }

        if (targetType == typeof(string))
        {
            return (T)(object)value.ToString()!;
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.Deserialize<T>()!;
        }

        return (T)Convert.ChangeType(value, targetType);
    }

    public static bool IsNullable(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}
