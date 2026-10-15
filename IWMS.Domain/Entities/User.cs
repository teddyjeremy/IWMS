using System;
using System.Collections.Generic;
using System.Text;
using IWMS.Domain.Common;

namespace IWMS.Domain.Entities
{
    public class User:BaseEntity
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime? LastLoginAtUtc { get; set; }
        public List<Role> Roles { get; set; } = new();
        public string FullName => $"{FirstName} {LastName}";
    }
}
