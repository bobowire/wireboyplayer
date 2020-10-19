using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WireboyPlayer
{
    public class TextBoxWriter : System.IO.TextWriter
    {
        TextBox txtBox;
        delegate void VoidAction();

        public TextBoxWriter(TextBox box)
        {
            txtBox = box; //transfer the enternal TextBox in
        }

        public override void Write(char value)
        {
            //base.Write(value);//still output to Console
            VoidAction action = delegate {
                txtBox.AppendText(value.ToString());
                if(txtBox.Visibility == Visibility.Visible)
                {
                    txtBox.ScrollToEnd();
                }
            };
            txtBox.Dispatcher.BeginInvoke(action);
        }

        public override System.Text.Encoding Encoding
        {
            get { return System.Text.Encoding.UTF8; }
        }
    }
}
