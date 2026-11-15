using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BaseConquest.Editor
{
    // Locate tokens rather than round-tripping the whole JSON, so unknown data survives a patch.
    internal sealed class CoreJsonSpans
    {
        internal sealed class Span {public int Start,Length;public char Kind;}
        private readonly string json;
        private readonly Dictionary<string,Span> spans=new Dictionary<string,Span>(StringComparer.Ordinal);
        private int position;
        private CoreJsonSpans(string json){this.json=json??throw new ArgumentNullException(nameof(json));if(json.Length>0&&json[0]=='\ufeff')position=1;}
        internal static Dictionary<string,Span> Parse(string json)
        {
            var parser=new CoreJsonSpans(json);parser.Value("",0);parser.White();
            if(parser.position!=json.Length)throw parser.Error();return parser.spans;
        }
        private InvalidOperationException Error()=>new InvalidOperationException("数值快照JSON结构非法，位置 "+position);
        private void White(){while(position<json.Length&&(json[position]==' '||json[position]=='\t'||json[position]=='\r'||json[position]=='\n'))position++;}
        private bool Take(char value){White();if(position<json.Length&&json[position]==value){position++;return true;}return false;}
        private void Need(char value){if(!Take(value))throw Error();}
        private static string Escape(string key)=>key.Replace("~","~0").Replace("/","~1");
        private void Value(string pointer,int depth)
        {
            if(depth>128)throw Error();White();if(position>=json.Length)throw Error();
            int start=position;char kind=json[position];
            if(kind=='{')
            {
                position++;var keys=new HashSet<string>(StringComparer.Ordinal);
                if(!Take('}'))do
                {
                    White();string key=String();if(!keys.Add(key))throw new InvalidOperationException("数值快照JSON含重复字段："+key);
                    Need(':');Value(pointer+"/"+Escape(key),depth+1);
                    if(Take('}'))break;Need(',');
                }while(true);
            }
            else if(kind=='[')
            {
                position++;int index=0;
                if(!Take(']'))do{Value(pointer+"/"+index++,depth+1);if(Take(']'))break;Need(',');}while(true);
            }
            else if(kind=='"')String();
            else if(kind=='t')Literal("true");
            else if(kind=='f')Literal("false");
            else if(kind=='n'){Literal("null");kind='l';}
            else{Number();kind='n';}
            spans.Add(pointer,new Span{Start=start,Length=position-start,Kind=kind});
        }
        private void Literal(string value)
        {if(position+value.Length>json.Length||string.CompareOrdinal(json,position,value,0,value.Length)!=0)throw Error();position+=value.Length;}
        private void Number()
        {
            if(position<json.Length&&json[position]=='-')position++;
            if(position>=json.Length)throw Error();
            if(json[position]=='0')position++;
            else{if(json[position]<'1'||json[position]>'9')throw Error();Digits();}
            if(position<json.Length&&json[position]=='.'){position++;RequiredDigits();}
            if(position<json.Length&&(json[position]=='e'||json[position]=='E'))
            {position++;if(position<json.Length&&(json[position]=='+'||json[position]=='-'))position++;RequiredDigits();}
        }
        private void Digits(){while(position<json.Length&&json[position]>='0'&&json[position]<='9')position++;}
        private void RequiredDigits(){int start=position;Digits();if(start==position)throw Error();}
        private string String()
        {
            if(position>=json.Length||json[position++]!='"')throw Error();var result=new StringBuilder();
            while(position<json.Length)
            {
                char value=json[position++];if(value=='"')return result.ToString();if(value<32)throw Error();
                if(value!='\\'){result.Append(value);continue;}
                if(position>=json.Length)throw Error();char escape=json[position++];
                switch(escape)
                {
                    case '"':case '\\':case '/':result.Append(escape);break;
                    case 'b':result.Append('\b');break;case 'f':result.Append('\f');break;
                    case 'n':result.Append('\n');break;case 'r':result.Append('\r');break;case 't':result.Append('\t');break;
                    case 'u':
                        if(position+4>json.Length||!ushort.TryParse(json.Substring(position,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out ushort code))throw Error();
                        result.Append((char)code);position+=4;break;
                    default:throw Error();
                }
            }
            throw Error();
        }
    }
}
