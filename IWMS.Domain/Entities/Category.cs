using IWMS.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace IWMS.Domain.Entities
{
    public class Category: BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<Product> Products { get; set; } = new();
    }
}
