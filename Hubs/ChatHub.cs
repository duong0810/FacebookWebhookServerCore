using Microsoft.AspNetCore.SignalR;

namespace FacebookWebhookServerCore.Hubs
{
    public class ChatHub : Hub
    {
        // Lớp này đóng vai trò là trung tâm giao tiếp.
        // Chúng ta không cần thêm phương thức ở đây vì chỉ cần đẩy tin nhắn từ server.
    }
}