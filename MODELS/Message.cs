namespace Webhook_Message.Models // Thay bằng namespace của project
{
    public class Message
    {
        public int Id { get; set; }
        public string SenderId { get; set; }
        public string Content { get; set; }
        public DateTime Time { get; set; }
    }
}