namespace Luban
{
    public class SerializationException : System.Exception
    {
        public SerializationException() { }
        public SerializationException(string msg) : base(msg) { }
    }
}
