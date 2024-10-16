using TomLonghurst.AsyncRedisClient.Extensions;

namespace TomLonghurst.AsyncRedisClient.Models.Commands;

public class MultiRedisCommand : IRedisCommand
{
    public byte[][] EncodedCommandList { get; }
    
    public string AsString => string.Join("|", EncodedCommandList.Select(x => x.AsSpan().AsString()));

    public MultiRedisCommand(IEnumerable<IRedisCommand> redisCommands)
    {
        EncodedCommandList = redisCommands.SelectMany(x => x.EncodedCommandList).ToArray();
    }
        
    public static MultiRedisCommand From(IEnumerable<IRedisCommand> redisCommands)
    {
        return new MultiRedisCommand(redisCommands);
    }
}