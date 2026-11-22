// 本工程同时引用了 WPF 与 WinForms（为了 System.Drawing 拼精灵图），
// 两者的隐式 using 会打架（Application / Image / Point / MouseEventArgs 重名），
// 因此关闭 ImplicitUsings，在这里显式声明需要的命名空间。
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
