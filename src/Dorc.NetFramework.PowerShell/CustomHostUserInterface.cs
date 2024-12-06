using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using System.Text;

namespace Dorc.NetFramework.PowerShell
{
    public class CustomHostUserInterface : PSHostUserInterface
    {
        public event EventHandler<MessageAddedEventArgs> MessageAdded;

        public override PSHostRawUserInterface RawUI { get; } = new CustomHostRawUserInterface();

        ~CustomHostUserInterface()
        {
            MessageAdded = null;
        }

        public override string ReadLine()
        {
            throw new NotImplementedException();
        }

        public override SecureString ReadLineAsSecureString()
        {
            throw new NotImplementedException();
        }

        public override void Write(string value)
        {
            RaiseMessageAddedEvent(value, MessageType.Info);
        }

        public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
        {
            Write(value);
        }

        public override void WriteLine(string message)
        {
            RaiseMessageAddedEvent(message, MessageType.Info);
        }

        public override void WriteErrorLine(string message)
        {
            RaiseMessageAddedEvent(message, MessageType.Error);
        }

        public override void WriteDebugLine(string message)
        {
            RaiseMessageAddedEvent(message, MessageType.Debug);
        }

        public override void WriteProgress(long sourceId, ProgressRecord record)
        {
            RaiseMessageAddedEvent($"PROGRESS: {sourceId} {record.PercentComplete}", MessageType.Info);
        }

        public override void WriteVerboseLine(string message)
        {
            RaiseMessageAddedEvent(message, MessageType.Verbose);
        }

        public override void WriteWarningLine(string message)
        {
            RaiseMessageAddedEvent(message, MessageType.Warning);
        }

        public override Dictionary<string, PSObject> Prompt(string caption, string message,
            Collection<FieldDescription> descriptions)
        {
            throw new NotImplementedException();
        }

        public override PSCredential PromptForCredential(string caption, string message, string userName,
            string targetName)
        {
            throw new NotImplementedException();
        }

        public override PSCredential PromptForCredential(string caption, string message, string userName,
            string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
        {
            throw new NotImplementedException();
        }

        public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices,
            int defaultChoice)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine($"PromptForChoice: caption:{caption}");
            stringBuilder.AppendLine($"PromptForChoice: message:{message}");
            stringBuilder.AppendLine("PromptForChoice: choices include.......");
            foreach (var choice in choices)
            {
                stringBuilder.AppendLine($"PromptForChoice: label:{choice.Label}, help:{choice.HelpMessage}");
            }

            stringBuilder.AppendLine("PromptForChoice: end of choices---------------");
            stringBuilder.AppendLine($"PromptForChoice: default:{defaultChoice}");
            RaiseMessageAddedEvent(stringBuilder.ToString(), MessageType.Info);
            return defaultChoice;
        }

        private void RaiseMessageAddedEvent(string message, MessageType messageType)
        {
            this.MessageAdded?.Invoke(this, new MessageAddedEventArgs(message, messageType));
        }
    }

    public class MessageAddedEventArgs
    {
        public string Message { get; set; }

        public MessageType MessageType { get; set; }

        public MessageAddedEventArgs(string message, MessageType messageType)
        {
            Message = message;
            MessageType = messageType;
        }   
    }

    public enum MessageType
    {
        None = -1,
        Info = 0,
        Debug = 1,
        Verbose = 2,
        Warning = 3,
        Error = 4
    }
}