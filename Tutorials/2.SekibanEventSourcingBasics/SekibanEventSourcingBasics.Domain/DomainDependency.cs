using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Sekiban.Core.Dependency;

namespace SekibanEventSourcingBasics.Domain
{
    internal class DomainDependency : DomainDependencyDefinitionBase
    {
        public override Assembly GetExecutingAssembly()
        {
            return Assembly.GetExecutingAssembly();
        }

        public override void Define()
        {
            throw new NotImplementedException();
        }
    }
}
