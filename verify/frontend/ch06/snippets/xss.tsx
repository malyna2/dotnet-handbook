function Comment({ text }: { text: string }) {
  return <p>{text}</p>;   // text becomes a text node: markup in it is shown, never parsed
}

function CommentUnsafe({ html }: { html: string }) {
  return <p dangerouslySetInnerHTML={{ __html: html }} />;   // parsed as HTML: an XSS sink
}

export { Comment, CommentUnsafe };
