﻿namespace Dorc.Api.Services
{
    public class WrongBuildTypeException : Exception
    {
        public WrongBuildTypeException()
        {

        }

        public WrongBuildTypeException(string message) : base(message)
        {

        }

        public WrongBuildTypeException(string message, Exception inner) : base(message, inner)
        {

        }
    }
}