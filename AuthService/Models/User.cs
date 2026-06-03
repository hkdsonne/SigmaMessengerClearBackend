using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using static System.Collections.Specialized.BitVector32;

namespace AuthService.Models
{
    public class User
    {
        public Guid id { get; set; } = Guid.NewGuid();
        public string username { get; set; } = string.Empty;
        public string password { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }

        public ICollection<Session> Sessions { get; set; } = new List<Session>();
    }
}
