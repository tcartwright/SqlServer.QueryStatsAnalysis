using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SqlServer.QueryStatsAnalysis.Library
{
    static class ExtensionMethods
    {
        internal static T TryGetAttributeValue<T>(this XElement ele, XName attributeName, T defaultValue)
        {

            if (ele == null) { return defaultValue; }
            var attr = ele.Attribute(attributeName);
            if (attr != null)
            {
                return (T)Convert.ChangeType(attr.Value, typeof(T));
            }
            return defaultValue;
        }
    }
}
