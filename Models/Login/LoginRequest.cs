using System.Data;

namespace BasementRPG.Models.Login
{
    public class LoginRequest
    {
        public string Username{get; set;}
        public string Password{get; set;}
        public bool Persistent{get; set;}
    }
}