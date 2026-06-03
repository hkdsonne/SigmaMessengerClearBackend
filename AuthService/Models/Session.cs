namespace AuthService.Models
{
    public class Session
    {
        public Guid id { get; set; } = Guid.NewGuid();
        public Guid user_id { get; set; }
        public string device_info { get; set; } = string.Empty;
        public string session_hash { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public DateTime expires_at { get; set; }

        public User? User { get; set; }
    }
}
