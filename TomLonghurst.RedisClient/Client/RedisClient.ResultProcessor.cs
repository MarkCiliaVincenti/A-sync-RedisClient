using TomLonghurst.RedisClient.Models;

namespace TomLonghurst.RedisClient.Client
{
    public partial class RedisClient
    {
        private SuccessResultProcessor SuccessResultProcessor => new SuccessResultProcessor();
        private DataResultProcessor DataResultProcessor => new DataResultProcessor();
        private IntegerResultProcessor IntegerResultProcessor => new IntegerResultProcessor();
        private FloatResultProcessor FloatResultProcessor => new FloatResultProcessor();
        private ArrayResultProcessor ArrayResultProcessor => new ArrayResultProcessor();
        private WordResultProcessor WordResultProcessor => new WordResultProcessor();

    }
}