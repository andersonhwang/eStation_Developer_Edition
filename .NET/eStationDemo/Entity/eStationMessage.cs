using eStationDemo.Enum;
using MessagePack;

namespace eStationDemo.Model
{
    [MessagePackObject]
    public class eStationMessage
    {
        /// <summary>
        /// Message code
        /// </summary>
        [Key(0)]
        public MessageCode Code { get; set; } = MessageCode.OK;
    }
}