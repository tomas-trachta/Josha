using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Josha.Models
{
    internal class DANamespaceBinding(string namespaceName, int keyCode)
    {
        public string NamespaceName { get; set; } = namespaceName;
        public int KeyCode { get; set; } = keyCode;
    }
}
