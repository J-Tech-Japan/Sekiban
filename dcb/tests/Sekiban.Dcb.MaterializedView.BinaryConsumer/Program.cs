using Sekiban.Dcb.MaterializedView;

var options = new MvOptions
{
    ServiceId = "binary-consumer"
};

Console.WriteLine(
    $"binary-consumer-ok:{options.ServiceId}:{typeof(IMvExecutor).FullName}");
