namespace Sekiban.Testing.Shared;

/// <summary>
///     Test constants
/// </summary>
public static class SekibanTestConstants
{
    /// <summary>
    ///     Category
    /// </summary>
    public const string Category = "Category";
    /// <summary>
    ///     Test Catetgories
    /// </summary>
    public static class Categories
    {
        /// <summary>
        ///     Use this category for tests that are learning
        /// </summary>
        public const string Learning = "Learning";
        /// <summary>
        ///     Use this category for tests that are flaky
        /// </summary>
        public const string Flaky = "Flaky";
        /// <summary>
        ///     Use this category for tests that are reproducing bugs
        /// </summary>
        public const string Bug = "Bug";
        /// <summary>
        ///     Use this category for tests that are performance tests
        /// </summary>
        public const string Performance = "Performance";
    }
}
