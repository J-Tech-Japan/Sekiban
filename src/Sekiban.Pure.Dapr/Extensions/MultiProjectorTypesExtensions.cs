using Sekiban.Pure.Projectors;

namespace Sekiban.Pure.Dapr.Extensions;

public static class MultiProjectorTypesExtensions
{
    /// <summary>
    /// Gets all multi-projector names that can be used as actor IDs
    /// </summary>
    public static IEnumerable<string> GetAllProjectorNames(this IMultiProjectorTypes multiProjectorTypes)
    {
        var projectorTypes = multiProjectorTypes.GetMultiProjectorTypes();
        
        foreach (var projectorType in projectorTypes)
        {
            // Create an instance to get the name
            if (Activator.CreateInstance(projectorType) is IMultiProjectorCommon projector)
            {
                var nameResult = multiProjectorTypes.GetMultiProjectorNameFromMultiProjector(projector);
                if (nameResult.IsSuccess)
                {
                    yield return nameResult.GetValue();
                }
            }
        }
    }
}