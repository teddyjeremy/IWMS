using System;
using System.Collections.Generic;
using System.Text;
using IWMS.Domain.Common;

namespace IWMS.Domain.Entities
{
    public class Role:BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<User> Users { get; set; } = new();
    }
}
