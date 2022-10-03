namespace Sekiban.EventSourcing.TestHelpers;

public interface IQueryFilterChecker<TProjectionDto>
{
    public void RegisterDto(TProjectionDto dto);
}
