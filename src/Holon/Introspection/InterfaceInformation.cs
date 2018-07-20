﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Holon.Introspection
{
    /// <summary>
    /// Represents interface information.
    /// </summary>
    [Serializable]
    [DebuggerDisplay("{Name}")]
    public class InterfaceInformation
    {
        /// <summary>
        /// Gets or sets the interface name.
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Gets or sets the available methods.
        /// </summary>
        public InterfaceMethodInformation[] Methods { get; set; }

        /// <summary>
        /// Gets or sets the available properties.
        /// </summary>
        public InterfacePropertyInformation[] Properties { get; set; }
        
        /// <summary>
        /// Gets the string representation of the interface information.
        /// </summary>
        /// <returns></returns>
        public override string ToString() {
            StringBuilder sb = new StringBuilder();
            sb.Append("interface ");
            sb.Append(Name);
            sb.AppendLine(" {");

            foreach(InterfaceMethodInformation method in Methods) {
                sb.Append("\t");
                sb.Append(method.ToString());
                sb.AppendLine(";");
            }

            if (Properties.Length > 0)
                sb.AppendLine();

            foreach (InterfacePropertyInformation property in Properties) {
                sb.Append("\t");
                sb.AppendLine(property.ToString());
                sb.AppendLine(";");
            }

            sb.Append("}");

            return sb.ToString();
        }
    }
}