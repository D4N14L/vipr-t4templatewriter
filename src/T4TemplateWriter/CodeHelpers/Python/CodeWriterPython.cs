using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Vipr.Core.CodeModel;

namespace Vipr.T4TemplateWriter.CodeHelpers.Python {
    public class CodeWriterPython : CodeWriterBase {

        public CodeWriterPython() : base() { }
        public CodeWriterPython(OdcmModel model) : base(model) { }

        public override String WriteOpeningCommentLine() {
            return "\'\'\'\n";
        }

        public override String WriteClosingCommentLine() {
            return "\n\'\'\'";
        }

        public override string WriteInlineCommentChar() {
            return "# ";
        }
    }
}
