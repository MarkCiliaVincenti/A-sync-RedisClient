using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TomLonghurst.RedisClient.Exceptions;
using TomLonghurst.RedisClient.Extensions;
using TomLonghurst.RedisClient.Models.Commands;

namespace TomLonghurst.RedisClient.Models
{
    public abstract class ResultProcessor
    {

    }

    public abstract class ResultProcessor<T> : ResultProcessor
    {
        private Client.RedisClient _redisClient;
        protected ReadResult ReadResult;
        protected PipeReader PipeReader;

        public IRedisCommand LastCommand { get => _redisClient.LastCommand; set => _redisClient.LastCommand = value; }

        public string LastAction { get => _redisClient.LastAction; set => _redisClient.LastAction = value; }

        private void SetMembers(Client.RedisClient redisClient, PipeReader pipeReader)
        {
            _redisClient = redisClient;
            PipeReader = pipeReader;
        }

        internal async ValueTask<T> Start(Client.RedisClient redisClient, PipeReader pipeReader)
        {
            SetMembers(redisClient, pipeReader);

            if (!PipeReader.TryRead(out ReadResult))
            {
                ReadResult = await PipeReader.ReadAsync().ConfigureAwait(false);
            }

            return await Process();
        }

        private protected abstract ValueTask<T> Process();

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected async ValueTask<Memory<byte>> ReadData()
        {
            var buffer = ReadResult.Buffer;

            if (buffer.IsEmpty && ReadResult.IsCompleted)
            {
                throw new UnexpectedRedisResponseException("Zero Length Response from Redis");
            }

            var line = await ReadLine();

            var firstChar = line.ItemAt(0);

            if (firstChar == '-')
            {
                throw new RedisFailedCommandException(await ReadLineAsStringAndAdvance(), LastCommand);
            }

            if (firstChar != '$')
            {
                throw new UnexpectedRedisResponseException($"Unexpected reply: {line}");
            }
            
            var alreadyReadToLineTerminator = false;
            long byteSizeOfData;
            if (line.Length == 5 && line.ItemAt(1) == '-' && line.ItemAt(2) == '1')
            {
                PipeReader.AdvanceTo(line.End);
                return null;
            }

            if(line.Length == 4)
            {
                byteSizeOfData = (long) char.GetNumericValue((char) line.ItemAt(1));
                PipeReader.AdvanceTo(line.End);
            }
            else
            {
                long.TryParse((await ReadLineAsStringAndAdvance()).Substring(1), out byteSizeOfData);
            }

            LastAction = "Reading Data Synchronously in ReadData";
            if (!PipeReader.TryRead(out ReadResult))
            {
                LastAction = "Reading Data Asynchronously in ReadData";
                ReadResult = await PipeReader.ReadAsync().ConfigureAwait(false);
            }

            buffer = ReadResult.Buffer;

            if (byteSizeOfData == 0)
            {
                throw new UnexpectedRedisResponseException("Invalid length");
            }
            
            var bytes = new byte[byteSizeOfData].AsMemory();

            buffer = buffer.Slice(0, Math.Min(byteSizeOfData, buffer.Length));

            var bytesReceived = buffer.Length;

            buffer.CopyTo(bytes.Slice(0, (int) bytesReceived).Span);

            if (buffer.Length == byteSizeOfData && ReadResult.Buffer.Length >= byteSizeOfData + 2)
            {
                alreadyReadToLineTerminator = true;
                PipeReader.AdvanceTo(ReadResult.Buffer.Slice(0, byteSizeOfData + 2).End);
            }
            else
            {
                PipeReader.AdvanceTo(buffer.End);
            }

            while (bytesReceived < byteSizeOfData)
            {
                LastAction = "Advancing Buffer in ReadData Loop";

                if ((ReadResult.IsCompleted || ReadResult.IsCanceled) && ReadResult.Buffer.IsEmpty)
                {
                    break;
                }

                LastAction = "Reading Data Synchronously in ReadData Loop";
                if (!PipeReader.TryRead(out ReadResult))
                {
                    LastAction = "Reading Data Asynchronously in ReadData Loop";
                    ReadResult = await PipeReader.ReadAsync().ConfigureAwait(false);
                }

                buffer = ReadResult.Buffer.Slice(0,
                    Math.Min(ReadResult.Buffer.Length, byteSizeOfData - bytesReceived));

                buffer
                    .CopyTo(bytes.Slice((int) bytesReceived,
                        (int) Math.Min(buffer.Length, byteSizeOfData - bytesReceived)).Span);

                bytesReceived += buffer.Length;

                if(bytesReceived == byteSizeOfData && ReadResult.Buffer.Length >= buffer.Length + 2)
                {
                    alreadyReadToLineTerminator = true;
                    PipeReader.AdvanceTo(ReadResult.Buffer.Slice(0, buffer.Length + 2).End);
                }
                else
                {
                    PipeReader.AdvanceTo(buffer.End);
                }
            }

            if (ReadResult.IsCompleted && ReadResult.Buffer.IsEmpty)
            {
                return bytes;
            }

            if (!alreadyReadToLineTerminator)
            {
                if (!PipeReader.TryRead(out ReadResult))
                {
                    LastAction = "Reading Data Asynchronously in ReadData Loop";
                    ReadResult = await PipeReader.ReadAsync().ConfigureAwait(false);
                }

                await PipeReader.AdvanceToLineTerminator(ReadResult);
            }

            return bytes;
        }

        protected async Task<ReadOnlySequence<byte>> ReadLine()
        {
            LastAction = "Finding End of Line Position";
            var endOfLinePosition = ReadResult.Buffer.GetEndOfLinePosition();
            if (endOfLinePosition == null)
            {
                LastAction = "Reading until End of Line found";

                ReadResult = await PipeReader.ReadUntilEndOfLineFound(ReadResult);

                LastAction = "Finding End of Line Position";
                endOfLinePosition = ReadResult.Buffer.GetEndOfLinePosition();
            }

            if (endOfLinePosition == null)
            {
                throw new RedisDataException("Can't find EOL in ReadLine");
            }

            var buffer = ReadResult.Buffer;

            return buffer.Slice(0, endOfLinePosition.Value);
        }

        protected async ValueTask<string> ReadLineAsStringAndAdvance()
        {
            var buffer = await ReadLine();
            
            var line = buffer.AsStringWithoutLineTerminators();

            LastAction = "Advancing Buffer to End of Line";
            PipeReader.AdvanceTo(buffer.End);

            return line;
        }
    }

    public class SuccessResultProcessor : ResultProcessor<object>
    {
        private protected override async ValueTask<object> Process()
        {
            var buffer = await ReadLine();
            
            if(buffer.ItemAt(0) == '-')
            {
                throw new RedisFailedCommandException(await ReadLineAsStringAndAdvance(), LastCommand);
            }
            
            PipeReader.AdvanceTo(buffer.End);
            
            return new object();
        }
    }

    public class DataResultProcessor : ResultProcessor<string>
    {
        private protected override async ValueTask<string> Process()
        {
            return (await ReadData()).AsString();
        }
    }

    public class WordResultProcessor : ResultProcessor<string>
    {
        private protected override async ValueTask<string> Process()
        {
            var bytes = await ReadLine();

            if (bytes.ItemAt(0) != '+')
            {
                throw new UnexpectedRedisResponseException(await ReadLineAsStringAndAdvance());
            }

            var word = bytes.Slice(1, bytes.Length - 1).AsStringWithoutLineTerminators();
            PipeReader.AdvanceTo(bytes.End);
            return word;
        }
    }

    public class IntegerResultProcessor : ResultProcessor<int>
    {
        private protected override async ValueTask<int> Process()
        {
            var line = await ReadLine();

            if (line.ItemAt(0) != ':')
            {
                throw new UnexpectedRedisResponseException(await ReadLineAsStringAndAdvance());
            }

            int number;
            if (line.Length == 4)
            {
                number = (int) char.GetNumericValue((char) line.ItemAt(1));
                PipeReader.AdvanceTo(line.End);
                return number;
            }
            
            if (line.Length == 5 && line.ItemAt(1) == '-' && line.ItemAt(2) == '1')
            {
                PipeReader.AdvanceTo(line.End);
                return -1;
            }
            
            var stringLine = line.Slice(1, line.Length - 1).AsStringWithoutLineTerminators();

            PipeReader.AdvanceTo(line.End);

            if (!int.TryParse(stringLine, out number))
            {
                throw new UnexpectedRedisResponseException(stringLine);
            }

            return number;
        }
    }

    public class FloatResultProcessor : ResultProcessor<float>
    {
        private protected override async ValueTask<float> Process()
        {
            var floatString = (await ReadData()).AsString();

            if (!float.TryParse(floatString, out var number))
            {
                throw new UnexpectedRedisResponseException(floatString);
            }

            return number;
        }
    }

    public class ArrayResultProcessor : ResultProcessor<IEnumerable<StringRedisValue>>
    {
        private protected override async ValueTask<IEnumerable<StringRedisValue>> Process()
        {

            var bytes = await ReadLine();

            if (bytes.ItemAt(0) != '*')
            {
                throw new UnexpectedRedisResponseException(await ReadLineAsStringAndAdvance());
            }

            int count;
            if (bytes.Length == 4)
            {
                count = (int) char.GetNumericValue((char) bytes.ItemAt(1));
            }
            else
            {
                count = int.Parse(bytes.Slice(1, bytes.Length - 1).AsStringWithoutLineTerminators());
            }
            
            PipeReader.AdvanceTo(bytes.End);

            var results = new byte [count][];
            for (var i = 0; i < count; i++)
            {
                // Refresh the pipe buffer before 'ReadData' method reads it
                LastAction = "Reading Data Synchronously in ExpectArray";
                if (!PipeReader.TryRead(out ReadResult))
                {
                    LastAction = "Reading Data Asynchronously in ExpectArray";
                    var readPipeTask = PipeReader.ReadAsync();
                    ReadResult = await readPipeTask.ConfigureAwait(false);
                }

                results[i] = (await ReadData()).ToArray();
            }

            return results.ToRedisValues();
        }
    }
}