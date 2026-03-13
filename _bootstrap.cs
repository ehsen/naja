
using System;
using System.Reflection;
using System.Windows.Forms;

[STAThread]
class NajaBootstrap
{
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        
        var asm = Assembly.LoadFrom("test_output.dll");
        var type = asm.GetType("test_output");
        var method = type?.GetMethod("Main");
        
        if (method != null)
        {
            method.Invoke(null, new object[] { args });
            return 0;
        }
        
        MessageBox.Show("Error: Main method not found", "Naja Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }
}
