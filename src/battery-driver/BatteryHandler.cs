using battery.driver.channels;
using battery.driver.models;
using l99.driver.@base;

namespace battery.driver;

public class BatteryHandler : Handler
{
    private readonly DataPublisher _publisher;

    public BatteryHandler(Machine machine, DataPublisher publisher) : base(machine)
    {
        _publisher = publisher;
    }

    public void Publish(ChannelRealData data) => _publisher.Publish(data);
    public void Publish(AlarmData data) => _publisher.Publish(data);
    public void Publish(ResultData data) => _publisher.Publish(data);
    public void Publish(StatusData data) => _publisher.Publish(data);
    public void Publish(WarningData data) => _publisher.Publish(data);
    public void Publish(AckData data) => _publisher.Publish(data);

    public DataPublisher Publisher => _publisher;
}
