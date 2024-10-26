using ResultBoxes;
namespace MemStat.Net;

public interface IMemoryUsageFinder
{
    public ResultBox<object> GetRawMemoryUsageObject();
    public ResultBox<UnitValue> ReceiveCurrentMemoryUsage();
    public ResultBox<double> GetTotalMemoryUsage();
    public ResultBox<double> GetMemoryUsagePercentage();
}
