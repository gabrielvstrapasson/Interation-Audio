using Brendha.Spike.Core.Models;
using System.Threading.Channels;

namespace Brendha.Spike.Server.Infrastructure
{
    public class AudioStreamBuffer
    {
        private readonly Channel<AudioPacket> _channel;

        public AudioStreamBuffer()
        {
            _channel = Channel.CreateUnbounded<AudioPacket>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        public async ValueTask WriteAsync(AudioPacket packet, CancellationToken cancellationToken = default)
        {
            await _channel.Writer.WriteAsync(packet, cancellationToken);
        }

        public IAsyncEnumerable<AudioPacket> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }

    }
}
