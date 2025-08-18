namespace Sekiban.Dcb.Orleans.Surrogates;

[GenerateSerializer]
public struct TagWriteReservationSurrogate
{
    [Id(0)]
    public string ReservationCode { get; set; }

    [Id(1)]
    public string ExpiredUTC { get; set; }

    [Id(2)]
    public string Tag { get; set; }
}
