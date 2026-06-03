namespace AuthService.Models
{
    public class UserMail
    {
        public Guid id { get; set; } = Guid.NewGuid();
        public Guid user_id { get; set; }
        public string email { get; set; } = string.Empty;
    }
}
