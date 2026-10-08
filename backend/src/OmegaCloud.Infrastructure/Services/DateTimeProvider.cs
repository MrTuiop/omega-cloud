using OmegaCloud.Application.Common.Interfaces;

namespace OmegaCloud.Infrastructure.Services;

public class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
