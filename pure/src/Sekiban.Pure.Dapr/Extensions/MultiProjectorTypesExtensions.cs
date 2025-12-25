using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Dapr.Extensions;

public static class MultiProjectorTypesExtensions
{
    /// <summary>
    ///     Gets all multi-projector names that can be used as actor IDs
    /// </summary>
    public static IEnumerable<string> GetAllProjectorNames(this IMultiProjectorTypes multiProjectorTypes)
    {
        var projectorTypes = multiProjectorTypes.GetMultiProjectorTypes();
        var names = new List<string>();

        foreach (var projectorType in projectorTypes)
        {
            // Use the new GenerateInitialPayload method to create instances
            var payloadResult = multiProjectorTypes.GenerateInitialPayload(projectorType);

            if (payloadResult.IsSuccess)
            {
                var projector = payloadResult.GetValue();
                var nameResult = multiProjectorTypes.GetMultiProjectorNameFromMultiProjector(projector);

                if (nameResult.IsSuccess)
                {
                    names.Add(nameResult.GetValue());
                }
            }
        }

        return names;
    }
}
