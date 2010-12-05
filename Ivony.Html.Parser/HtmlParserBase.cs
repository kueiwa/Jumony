﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ivony.Html.Parser.ContentModels;

namespace Ivony.Html.Parser
{
  public abstract class HtmlParserBase : IHtmlParser
  {


    private static readonly IDictionary<string, Regex> endTagRegexes = new Dictionary<string, Regex>( StringComparer.InvariantCultureIgnoreCase );


    static HtmlParserBase()
    {
      foreach ( var tagName in HtmlSpecification.cdataTags )//为所有的CDATA标签准备匹配结束标记的正则表达式
      {
        endTagRegexes.Add( tagName, new Regex( @"</#tagName\s*>".Replace( "#tagName", tagName ), RegexOptions.IgnoreCase | RegexOptions.Compiled ) );
      }
    }


    /// <summary>
    /// 容器堆栈
    /// </summary>
    protected Stack<IHtmlContainer> ContainerStack
    {
      get;
      private set;
    }

    /// <summary>
    /// 初始化容器堆栈
    /// </summary>
    protected virtual void InitializeStack()
    {
      ContainerStack = new Stack<IHtmlContainer>();
    }


    /// <summary>
    /// 派生类提供 HTML 文本读取器
    /// </summary>
    /// <param name="html">要读取分析的 HTML 文本</param>
    /// <returns>文本读取器</returns>
    protected abstract IHtmlReader CreateReader( string html );


    /// <summary>
    /// 当前在使用的 HTML 文本读取器
    /// </summary>
    protected IHtmlReader Reader
    {
      get;
      private set;
    }


    /// <summary>
    /// 派生类提供 Provider 用于创建 DOM 结构
    /// </summary>
    protected abstract IHtmlDomProvider Provider
    {
      get;
    }


    /// <summary>
    /// 当前容器
    /// </summary>
    protected IHtmlContainer CurrentContainer
    {
      get { return ContainerStack.Peek(); }
    }



    private readonly object _sync = new object();


    /// <summary>
    /// 用于同步的对象，在任何公开方法中应lock，确保分析器始终只在一个线程中运行
    /// </summary>
    protected object SyncRoot
    {
      get { return _sync; }
    }

    /// <summary>
    /// 分析 HTML 文本并创建文档
    /// </summary>
    /// <param name="html">HTML 文本</param>
    /// <returns>分析好的 HTML 文档</returns>
    public virtual IHtmlDocument Parse( string html )
    {

      lock ( SyncRoot )
      {

        InitializeStack();

        var document = Provider.CreateDocument();

        if ( string.IsNullOrEmpty( html ) )
          return document;

        ContainerStack.Push( document );

        ParseInternal( html );

        return document;

      }
    }

    /// <summary>
    /// 分析 HTML 文本
    /// </summary>
    /// <param name="html">要分析的 HTML 文本</param>
    protected void ParseInternal( string html )
    {


      Reader = CreateReader( html );

      foreach ( var fragment in Reader.EnumerateContent() )
      {

        var text = fragment as HtmlTextContent;
        if ( text != null )
          ProcessText( text );

        var beginTag = fragment as HtmlBeginTag;
        if ( beginTag != null )
          ProcessBeginTag( beginTag );

        var endTag = fragment as HtmlEndTag;
        if ( endTag != null )
          ProcessEndTag( endTag );

        var comment = fragment as HtmlCommentContent;
        if ( comment != null )
          ProcessComment( comment );

        var special = fragment as HtmlSpecialTag;
        if ( special != null )
          ProcessSpecial( special );

      }
    }



    /// <summary>
    /// 处理文本节点
    /// </summary>
    /// <param name="text">HTML文本信息</param>
    protected virtual void ProcessText( HtmlTextContent text )
    {
      CreateTextNode( text.Html );
    }

    /// <summary>
    /// 创建文本节点添加到当前容器
    /// </summary>
    /// <param name="text">HTML 文本</param>
    /// <returns></returns>
    protected virtual IHtmlTextNode CreateTextNode( string text )
    {
      return Provider.AddTextNode( CurrentContainer, CurrentContainer.Nodes().Count(), text );
    }



    /// <summary>
    /// 处理元素开始标签
    /// </summary>
    /// <param name="beginTag">开始标签信息</param>
    protected virtual void ProcessBeginTag( HtmlBeginTag beginTag )
    {
      string tagName = beginTag.TagName;
      bool selfClosed = beginTag.SelfClosed;

      //检查是否为自结束标签，并作相应处理
      if ( IsSelfCloseElement( beginTag ) )
        selfClosed = true;


      //检查是否为CData标签，并作相应处理
      if ( IsCDataElement( beginTag ) )
        Reader.EnterCDataMode( tagName.ToLowerInvariant() );



      //检查父标签是否可选结束标记，并作相应处理
      {
        var element = CurrentContainer as IHtmlElement;
        if ( element != null && HtmlSpecification.optionalCloseTags.Contains( element.Name, StringComparer.InvariantCultureIgnoreCase ) )
        {
          if ( ImmediatelyClose( tagName, element ) )
            ContainerStack.Pop();
        }
      }




      //处理所有属性
      var attributes = new Dictionary<string, string>( StringComparer.InvariantCultureIgnoreCase );

      foreach ( var a in beginTag.Attributes )
      {
        string name = a.Name;
        string value = a.Value;

        value = HtmlEncoding.HtmlDecode( value );

        if ( attributes.ContainsKey( name ) )//重复的属性名，只取第一个
          continue;

        attributes.Add( name, value );
      }




      //创建元素
      {
        var element = CreateElement( tagName, attributes );


        //加入容器堆栈
        if ( !selfClosed )
          ContainerStack.Push( element );
      }
    }


    /// <summary>
    /// 检查元素是否为自结束标签
    /// </summary>
    /// <param name="tag">元素开始标签</param>
    /// <returns>是否为自结束标签</returns>
    protected virtual bool IsSelfCloseElement( HtmlBeginTag tag )
    {
      return HtmlSpecification.selfCloseTags.Contains( tag.TagName, StringComparer.InvariantCultureIgnoreCase );
    }

    /// <summary>
    /// 检查元素是否为CDATA标签
    /// </summary>
    /// <param name="tag">元素开始标签</param>
    /// <returns>是否为CDATA标签</returns>
    protected virtual bool IsCDataElement( HtmlBeginTag tag )
    {
      return HtmlSpecification.cdataTags.Contains( tag.TagName, StringComparer.InvariantCultureIgnoreCase );
    }



    /// <summary>
    /// 检查当前开放的可选结束标签是否必须立即关闭
    /// </summary>
    /// <param name="tagName">分析器遇到的标签</param>
    /// <param name="element">当前开放的可选结束标签</param>
    /// <returns></returns>
    protected virtual bool ImmediatelyClose( string tagName, IHtmlElement element )
    {
      return HtmlSpecification.ImmediatelyClose( element.Name, tagName );
    }


    /// <summary>
    /// 创建元素并加入当前容器
    /// </summary>
    /// <param name="tagName">元素名</param>
    /// <param name="attributes">元素属性集合</param>
    /// <returns>创建好的元素</returns>
    protected virtual IHtmlElement CreateElement( string tagName, Dictionary<string, string> attributes )
    {
      return Provider.AddElement( CurrentContainer, CurrentContainer.Nodes().Count(), tagName, attributes );
    }




    /// <summary>
    /// 处理结束标签
    /// </summary>
    /// <param name="match">结束标签信息</param>
    protected virtual void ProcessEndTag( HtmlEndTag endTag )
    {
      var tagName = endTag.TagName;


      if ( ContainerStack.OfType<DomElement>().Select( e => e.Name ).Contains( tagName, StringComparer.InvariantCultureIgnoreCase ) )
      {
        while ( true )
        {
          var element = ContainerStack.Pop() as DomElement;
          if ( element.Name.Equals( tagName, StringComparison.InvariantCultureIgnoreCase ) )
            break;
        }
      }
      else
      {
        ProcessEndTagMissingBeginTag( endTag );
      }

      //无需退出CData标签，读取器会自动退出
    }

    /// <summary>
    /// 处理丢失了开始标签的结束标签
    /// </summary>
    /// <param name="match">结束标签信息</param>
    protected virtual void ProcessEndTagMissingBeginTag( HtmlEndTag endTag )
    {
      //如果堆栈中没有对应的开始标签，则将这个结束标签解释为文本
      CreateTextNode( endTag.Html );
    }





    /// <summary>
    /// 处理 HTML 注释
    /// </summary>
    /// <param name="match">HTML 注释信息</param>
    protected virtual void ProcessComment( HtmlCommentContent comment )
    {
      CreateCommet( comment.Comment );
    }


    /// <summary>
    /// 创建注释节点并加入当前容器
    /// </summary>
    /// <param name="comment">注释内容</param>
    /// <returns>创建的注释节点</returns>
    protected virtual IHtmlComment CreateCommet( string comment )
    {
      return Provider.AddComment( CurrentContainer, CurrentContainer.Nodes().Count(), comment );
    }



    /// <summary>
    /// 处理特殊节点
    /// </summary>
    /// <param name="match">特殊节点的匹配</param>
    /// <returns>创建的特殊节点的匹配</returns>
    protected virtual IHtmlSpecial ProcessSpecial( HtmlSpecialTag special )
    {
      return null;
    }

  }
}
