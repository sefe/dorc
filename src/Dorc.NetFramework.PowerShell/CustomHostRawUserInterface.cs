using System;
using System.Management.Automation.Host;

namespace Dorc.NetFramework.PowerShell
{
    public class CustomHostRawUserInterface : PSHostRawUserInterface
    {
        private readonly Size _bufferSize = new Size(5000, 5000);

        public override ConsoleColor ForegroundColor
        {
            get => Console.ForegroundColor;
            set => throw new NotImplementedException();
        }

        public override ConsoleColor BackgroundColor
        {
            get => Console.BackgroundColor;
            set => throw new NotImplementedException();
        }

        public override Coordinates CursorPosition
        {
            get => new Coordinates(0, 0);
            set => throw new NotImplementedException();
        }

        public override Coordinates WindowPosition
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override int CursorSize
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override Size BufferSize
        {
            get => _bufferSize;
            set => throw new NotImplementedException();
        }

        public override Size WindowSize
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override Size MaxWindowSize => throw new NotImplementedException();

        public override Size MaxPhysicalWindowSize => throw new NotImplementedException();

        public override bool KeyAvailable => throw new NotImplementedException();

        public override string WindowTitle
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override KeyInfo ReadKey(ReadKeyOptions options)
        {
            throw new NotImplementedException();
        }

        public override void FlushInputBuffer()
        {
        }

        public override void SetBufferContents(Coordinates origin, BufferCell[,] contents)
        {
        }

        public override void SetBufferContents(Rectangle rectangle, BufferCell fill)
        {
        }

        public override BufferCell[,] GetBufferContents(Rectangle rectangle)
        {
            throw new NotImplementedException();
        }

        public override void ScrollBufferContents(Rectangle source, Coordinates destination, Rectangle clip,
            BufferCell fill)
        {
        }
    }
}